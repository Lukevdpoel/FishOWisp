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
        public bool isSprinting;
    }

    private struct CameraPose
    {
        public Vector3 position;
        public Quaternion rotation;
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

    [Header("Bobber Follow Camera Settings")]
    [Tooltip("Height above the bobber the camera floats while following.")]
    [SerializeField] private float bobberFollowHeight = 1.8f;
    [Tooltip("Horizontal distance from the bobber, placed on the player side so the camera looks back toward the bobber from the player's general direction.")]
    [SerializeField] private float bobberFollowDistance = 3f;
    [Tooltip("How fast the camera blends INTO the bobber pose when the bobber lands in water. Higher = snappier.")]
    [SerializeField] private float bobberFollowLerpSpeed = 4f;
    [Tooltip("Seconds the camera takes to snap from the in-water pose back to the player orbit pose when the reel button is pressed. Keep low so the camera lands at the player well before the bobber arc finishes.")]
    [SerializeField] private float bobberReturnDuration = 0.35f;

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

    [Header("Nibble Reaction Pitch")]
    [Tooltip("Degrees to pitch the camera down on each individual nibble. Positive = look down.")]
    public float nibblePitchOffset = 5f;
    [Tooltip("How long the nibble pitch is held before releasing back to neutral.")]
    public float nibblePitchHoldTime = 0.25f;
    [Tooltip("Lerp speed for the nibble pitch offset to reach its target.")]
    public float nibblePitchLerpSpeed = 6f;

    [Header("Bite Reaction Zoom")]
    [Tooltip("Degrees subtracted from base FOV while a fish is biting (reaction window). Larger = stronger zoom-in. Holds until success or failure.")]
    public float biteFovZoom = 15f;
    [Tooltip("How fast (per second) the FOV interpolates toward its target during the bite zoom.")]
    public float biteFovLerpSpeed = 8f;

    [Header("Sprint FOV")]
    [Tooltip("Degrees added to the camera's base FOV while the player is sprinting.")]
    [SerializeField] private float sprintFovBoost = 8f;
    [Tooltip("How fast (per second) the FOV interpolates toward its target. Higher = snappier.")]
    [SerializeField] private float sprintFovLerpSpeed = 6f;

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
    private bool isBiteActive = false;
    private float currentPitchOffset = 0f;

    private float chargeProgressTarget = 0f;
    private float chargeProgress = 0f;
    private float chargeProgressVel = 0f;

    private float baseFov;

    // Two-rig camera system: orbit code writes to playerCameraPose, bobber-follow code writes to
    // bobberCameraPose, and the actual cameraTransform interpolates between them by bobberCameraBlend.
    private CameraPose playerCameraPose;
    private CameraPose bobberCameraPose;
    private bool hasBobberCameraPose;
    private float bobberCameraBlend;
    private float bobberCameraBlendTarget;

    private Transform bobberFollowTarget;
    private BobberController bobberFollowController;

    // Dedicated reel-press return: a one-shot fixed-duration lerp from the snapshot in-water pose
    // back to the player orbit pose. Bypasses the bobber blend so the camera can't appear to track
    // the bobber on its way back.
    private bool isReturningToPlayer;
    private CameraPose returnFromPose;
    private float returnT;

    public Transform CameraTransform => (useStaticCamera && staticCameraTarget != null) ? staticCameraTarget : cameraTransform;

    public void SetCatchCamera(bool active) => isCatchCameraActive = active;

    private void OnEnable()
    {
        FishingEvents.OnFishNibble += HandleFishNibble;
        FishingEvents.OnFishBite += HandleFishBite;
        FishingEvents.OnHookFishSuccess += HandleBiteRelease;
        FishingEvents.OnCancelFishing += HandleBiteRelease;

        FishingEvents.OnChargeProgressNormalized += HandleChargeProgress;
        FishingEvents.OnCancelCharging += HandleChargeEnded;
        FishingEvents.OnThrowBobber += HandleChargeReleased;
        FishingEvents.OnCancelFishing += HandleChargeEnded;

        FishingEvents.OnBobberLandedInWater += HandleBobberLanded;
        FishingEvents.OnStartReeling += HandleStartReeling;
        FishingEvents.OnReelingCompleted += HandleBobberFollowEnd;
        FishingEvents.OnCancelFishing += HandleBobberFollowEnd;
    }

    private void OnDisable()
    {
        FishingEvents.OnFishNibble -= HandleFishNibble;
        FishingEvents.OnFishBite -= HandleFishBite;
        FishingEvents.OnHookFishSuccess -= HandleBiteRelease;
        FishingEvents.OnCancelFishing -= HandleBiteRelease;

        FishingEvents.OnChargeProgressNormalized -= HandleChargeProgress;
        FishingEvents.OnCancelCharging -= HandleChargeEnded;
        FishingEvents.OnThrowBobber -= HandleChargeReleased;
        FishingEvents.OnCancelFishing -= HandleChargeEnded;

        FishingEvents.OnBobberLandedInWater -= HandleBobberLanded;
        FishingEvents.OnStartReeling -= HandleStartReeling;
        FishingEvents.OnReelingCompleted -= HandleBobberFollowEnd;
        FishingEvents.OnCancelFishing -= HandleBobberFollowEnd;
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
    private void HandleFishBite(BobberController b) { isBiteActive = true; nibbleHoldTimer = 0f; }
    private void HandleBiteRelease() { isBiteActive = false; }

    private void HandleBobberLanded(BobberController b)
    {
        if (b == null) return;
        // Any new bobber cast cancels a pending reel-return; only happens if the player throws
        // again before the previous return finished.
        isReturningToPlayer = false;
        bobberFollowController = b;
        bobberFollowTarget = b.transform;
        bobberCameraBlendTarget = 1f;
    }

    // On an empty-line reel start: tear the bobber rig down completely. With blend = 0, no return
    // lerp, and no bobber pose, the next UpdateCamera frame sets cameraTransform = playerCameraPose
    // directly — a hard snap to the player orbit pose. Smoothing can be added back later once this
    // baseline behavior is verified working. When a fish is hooked we keep the bobber camera live
    // until the catch camera takes over.
    private void HandleStartReeling()
    {
        if (bobberFollowTarget == null) return;
        if (bobberFollowController == null || bobberFollowController.HookedFish == null)
        {
            bobberCameraBlend = 0f;
            bobberCameraBlendTarget = 0f;
            hasBobberCameraPose = false;
            bobberFollowTarget = null;
            bobberFollowController = null;
            isReturningToPlayer = false;
        }
    }

    private void HandleBobberFollowEnd()
    {
        // Fade the blend out (used for the hooked-fish path; empty-line path already cleared it).
        bobberCameraBlendTarget = 0f;
    }

    private void UpdateNibblePitchOffset()
    {
        if (nibbleHoldTimer > 0f) nibbleHoldTimer -= Time.deltaTime;

        float target = nibbleHoldTimer > 0f ? nibblePitchOffset : 0f;
        currentPitchOffset = Mathf.Lerp(currentPitchOffset, target, Time.deltaTime * nibblePitchLerpSpeed);
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
            if (cam != null) baseFov = cam.fieldOfView;
            currentPivotPosition = playerModel.position + Vector3.up * pivotHeight;
            playerCameraPose.position = cameraTransform.position;
            playerCameraPose.rotation = cameraTransform.rotation;
        }
        if (framingTarget == null) framingTarget = playerModel;
    }

    public void UpdateCamera(CameraInput input)
    {
        if (!cameraTransform) return;

        // Static-camera path runs even if playerModel is unassigned, so HandleMovement gets a
        // valid camera-relative basis from cameraTransform.forward/right.
        if (useStaticCamera)
        {
            if (staticCameraTarget != null) { cameraTransform.position = staticCameraTarget.position; cameraTransform.rotation = staticCameraTarget.rotation; }
            return;
        }

        if (input.playerModel == null) return;
        if (InventoryUI.IsInventoryOpen) return;

        ComputePlayerCameraPose(input);

        if (bobberFollowTarget != null)
        {
            ComputeBobberCameraPose(input);
            hasBobberCameraPose = true;
        }

        // Dedicated reel-press return takes priority over the blend system. Lerp directly from
        // the snapshot pose to the live player orbit pose over bobberReturnDuration.
        if (isReturningToPlayer)
        {
            float dur = Mathf.Max(bobberReturnDuration, 0.0001f);
            returnT += Time.deltaTime / dur;
            if (returnT >= 1f)
            {
                cameraTransform.position = playerCameraPose.position;
                cameraTransform.rotation = playerCameraPose.rotation;
                isReturningToPlayer = false;
            }
            else
            {
                float t = returnT * returnT * (3f - 2f * returnT); // smoothstep ease
                cameraTransform.position = Vector3.Lerp(returnFromPose.position, playerCameraPose.position, t);
                cameraTransform.rotation = Quaternion.Slerp(returnFromPose.rotation, playerCameraPose.rotation, t);
            }
            UpdateFov(input);
            return;
        }

        float blendT = 1f - Mathf.Exp(-bobberFollowLerpSpeed * Time.deltaTime);
        bobberCameraBlend = Mathf.Lerp(bobberCameraBlend, bobberCameraBlendTarget, blendT);

        if (hasBobberCameraPose && bobberCameraBlend > 0.001f)
        {
            cameraTransform.position = Vector3.Lerp(playerCameraPose.position, bobberCameraPose.position, bobberCameraBlend);
            cameraTransform.rotation = Quaternion.Slerp(playerCameraPose.rotation, bobberCameraPose.rotation, bobberCameraBlend);
        }
        else
        {
            cameraTransform.position = playerCameraPose.position;
            cameraTransform.rotation = playerCameraPose.rotation;

            if (bobberCameraBlendTarget <= 0.001f && hasBobberCameraPose)
            {
                bobberCameraBlend = 0f;
                hasBobberCameraPose = false;
                bobberFollowTarget = null;
                bobberFollowController = null;
            }
        }

        UpdateFov(input);
    }

    private void ComputePlayerCameraPose(CameraInput input)
    {
        if (float.IsNaN(xVel) || float.IsNaN(yVel) || float.IsNaN(distanceVelocity)) { xVel = 0f; yVel = 0f; distanceVelocity = 0f; }

        bool isDialogueCamera = input.areControlsLocked && DialogueManager.Instance != null && DialogueManager.Instance.IsDialogueActive();
        bool isBoardCamera = input.areControlsLocked && input.isBountyBoardActive && input.activeBountyBoard != null;
        // While the bobber camera is dominant OR we're in the middle of a reel-return lerp, freeze
        // the player's mouse-driven orbit so the pose the camera lands on is exactly the pre-throw
        // aim direction.
        bool bobberCameraDominant = bobberCameraBlend > 0.01f || isReturningToPlayer;

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
        else if (!input.isFightingFish && !isCatchCameraActive && !input.areControlsLocked && !bobberCameraDominant)
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

        UpdateNibblePitchOffset();

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

        playerCameraPose.position = currentPivotPosition + cameraDirection * currentCameraDistance;
        playerCameraPose.rotation = rotation;
    }

    private void ComputeBobberCameraPose(CameraInput input)
    {
        // Preferred path: read the pose straight off a child anchor Transform on the bobber prefab.
        // This lets designers place the in-water camera visually instead of tuning numeric offsets.
        Transform anchor = bobberFollowController != null ? bobberFollowController.CameraAnchor : null;
        if (anchor != null)
        {
            bobberCameraPose.position = anchor.position;
            bobberCameraPose.rotation = anchor.rotation;
            return;
        }

        // Fallback: derive a pose from bobberFollowHeight / bobberFollowDistance when no anchor is set.
        Vector3 bobberPos = bobberFollowTarget.position;
        Vector3 playerPos = input.playerModel.position;

        Vector3 toBobber = bobberPos - playerPos;
        toBobber.y = 0f;
        Vector3 awayFromBobber;
        if (toBobber.sqrMagnitude > 0.0001f)
        {
            awayFromBobber = -toBobber.normalized;
        }
        else
        {
            Vector3 fwd = input.playerModel.forward;
            fwd.y = 0f;
            awayFromBobber = fwd.sqrMagnitude > 0.0001f ? -fwd.normalized : -Vector3.forward;
        }

        Vector3 targetPos = bobberPos + Vector3.up * bobberFollowHeight + awayFromBobber * bobberFollowDistance;
        Vector3 lookDir = bobberPos - targetPos;
        Quaternion targetRot = lookDir.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(lookDir.normalized)
            : cameraTransform.rotation;

        bobberCameraPose.position = targetPos;
        bobberCameraPose.rotation = targetRot;
    }

    private void UpdateFov(CameraInput input)
    {
        if (cam == null) cam = cameraTransform.GetComponent<Camera>();
        if (cam == null) return;

        float targetFov;
        float fovLerpSpeed;
        if (isBiteActive)
        {
            targetFov = baseFov - biteFovZoom;
            fovLerpSpeed = biteFovLerpSpeed;
        }
        else
        {
            targetFov = baseFov + (input.isSprinting ? sprintFovBoost : 0f);
            fovLerpSpeed = sprintFovLerpSpeed;
        }
        cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, targetFov, Time.deltaTime * fovLerpSpeed);
    }
}
