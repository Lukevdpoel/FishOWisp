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
    [Tooltip("Degrees per second the camera yaws at full right-stick deflection.")]
    [SerializeField] private float gamepadLookSpeedX = 150f;
    [Tooltip("Degrees per second the camera pitches at full right-stick deflection.")]
    [SerializeField] private float gamepadLookSpeedY = 90f;
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
    [Tooltip("Height above the bobber the camera pivots around while orbiting in water.")]
    [SerializeField] private float bobberFollowHeight = 1.8f;
    [Tooltip("Orbit radius around the bobber while in water. Mouse drives the orbit angle.")]
    [SerializeField] private float bobberFollowDistance = 3f;
    [Tooltip("How fast the camera blends INTO the bobber pose when the bobber lands in water. Higher = snappier.")]
    [SerializeField] private float bobberFollowLerpSpeed = 4f;
    [Tooltip("Seconds the camera takes to snap from the in-water pose back to the player orbit pose when the reel button is pressed. Keep low so the camera lands at the player well before the bobber arc finishes.")]
    [SerializeField] private float bobberReturnDuration = 0.35f;
    [Tooltip("When true, the in-water camera orbits the bobber using mouse input. When false, falls back to the bobber prefab's CameraAnchor (or a fixed pose) like the old behavior.")]
    [SerializeField] private bool bobberOrbitWithMouse = true;
    [Tooltip("Minimum height the in-water camera stays above the water surface. Prevents the camera from clipping through the water plane when pitched down.")]
    [SerializeField] private float bobberCameraWaterClearance = 0.4f;

    [Header("Fight Camera Framing (over-shoulder)")]
    [Tooltip("Side offset (m) from the player while fighting a fish. Positive = right side of the player (relative to the player→bobber line).")]
    [SerializeField] private float fightShoulderSide = 1.4f;
    [Tooltip("How far back behind the player the over-shoulder camera sits.")]
    [SerializeField] private float fightShoulderBack = 1.2f;
    [Tooltip("Height above the player's feet for the over-shoulder camera.")]
    [SerializeField] private float fightShoulderHeight = 1.7f;
    [Tooltip("Where the camera aims along the player→bobber line, 0 = at the player, 1 = at the bobber. ~0.4 frames both nicely.")]
    [Range(0f, 1f)]
    [SerializeField] private float fightFramingAimT = 0.4f;
    [Tooltip("Lerp speed for the over-shoulder pose. Higher = snappier transition into the over-shoulder framing when the fight starts.")]
    [SerializeField] private float fightFramingLerpSpeed = 4f;

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
    private float currentCameraDistance, distanceVelocity;
    private Vector3 currentPivotPosition;
    private Vector3 pivotVelocity;

    private readonly CameraFovTracker fovTracker = new CameraFovTracker();
    private readonly CameraReactionTracker reactionTracker = new CameraReactionTracker();
    private readonly BobberCameraTracker bobberTracker = new BobberCameraTracker();
    private readonly FightFramingHelper fightFraming = new FightFramingHelper();

    // Player-side orbit pose; bobber side lives in bobberTracker.
    private CameraPose playerCameraPose;

    // Smoothed over-shoulder fight pose. Ramps from the orbit pose into the framing pose so the
    // transition into a fish fight is smooth instead of a snap.

    public Transform CameraTransform => (useStaticCamera && staticCameraTarget != null) ? staticCameraTarget : cameraTransform;

    // True while the in-water bobber/lure camera owns the view — the mouse is orbiting the
    // bobber, so gameplay cursor rules (locked + hidden) should apply even though player
    // controls are locked for fishing.
    public bool IsBobberCameraActive => bobberTracker.BobberCameraDominant;

    public void SetCatchCamera(bool active) => isCatchCameraActive = active;

    private void OnEnable()
    {
        FishingEvents.OnFishNibble += HandleFishNibble;
        FishingEvents.OnFishBite += HandleFishBite;
        FishingEvents.OnHookFishSuccess += HandleBiteRelease;
        FishingEvents.OnCancelFishing += HandleBiteRelease;
        // Reeling always returns the camera to neutral. Without this, a bite the rod ignores
        // (e.g. a lure strike landing the same moment the player reels in) zooms the FOV in
        // with no event left to ever release it.
        FishingEvents.OnStartReeling += HandleBiteRelease;

        FishingEvents.OnChargeProgressNormalized += HandleChargeProgress;
        FishingEvents.OnCancelCharging += HandleChargeEnded;
        FishingEvents.OnThrowBobber += HandleChargeReleased;
        FishingEvents.OnCancelFishing += HandleChargeEnded;

        FishingEvents.OnBobberLandedInWater += HandleBobberLanded;
        FishingEvents.OnStartReeling += HandleStartReeling;
        FishingEvents.OnHookFishSuccess += HandleBobberFollowEnd;
        FishingEvents.OnStartReeling += HandleBobberFollowEnd;
        FishingEvents.OnCancelFishing += HandleBobberFollowEnd;
    }

    private void OnDisable()
    {
        FishingEvents.OnFishNibble -= HandleFishNibble;
        FishingEvents.OnFishBite -= HandleFishBite;
        FishingEvents.OnHookFishSuccess -= HandleBiteRelease;
        FishingEvents.OnCancelFishing -= HandleBiteRelease;
        FishingEvents.OnStartReeling -= HandleBiteRelease;

        FishingEvents.OnChargeProgressNormalized -= HandleChargeProgress;
        FishingEvents.OnCancelCharging -= HandleChargeEnded;
        FishingEvents.OnThrowBobber -= HandleChargeReleased;
        FishingEvents.OnCancelFishing -= HandleChargeEnded;

        FishingEvents.OnBobberLandedInWater -= HandleBobberLanded;
        FishingEvents.OnStartReeling -= HandleStartReeling;
        FishingEvents.OnHookFishSuccess -= HandleBobberFollowEnd;
        FishingEvents.OnStartReeling -= HandleBobberFollowEnd;
        FishingEvents.OnCancelFishing -= HandleBobberFollowEnd;
    }

    private void HandleChargeProgress(float t)
    {
        reactionTracker.SetChargeProgress(t);
        if (!hasLoggedChargeEvent)
        {
            hasLoggedChargeEvent = true;
            Debug.Log($"[ChargeCam] First charge event received. zoomAmount={chargeZoomAmount}, pitchUp={chargePitchUpAngle}, startDistance={startDistance}");
        }
    }
    private void HandleChargeEnded() { reactionTracker.ClearChargeProgress(); }
    private void HandleChargeReleased(Vector3 dir, float force) { reactionTracker.ClearChargeProgress(); }
    private bool hasLoggedChargeEvent;

    private void HandleFishNibble(BobberController b) { reactionTracker.OnNibble(nibblePitchHoldTime); }
    private void HandleFishBite(BobberController b) { fovTracker.OnBiteStart(); reactionTracker.OnBite(); }
    private void HandleBiteRelease() { fovTracker.OnBiteRelease(); }

    private void HandleBobberLanded(BobberController b) => bobberTracker.OnBobberLanded(b);

    // Empty-line reel: tear the bobber rig down. Hooked-fish path keeps the rig live until the
    // catch camera takes over.
    private void HandleStartReeling()
    {
        if (!bobberTracker.HasFollowTarget) return;
        if (!bobberTracker.HasHookedFish) bobberTracker.HardClear();
    }

    private void HandleBobberFollowEnd() => bobberTracker.OnFollowEnd();

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
            fovTracker.Initialize(cameraTransform.GetComponent<Camera>());
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
        if (InventoryUI.IsInventoryOpen || NoteMenu.IsNotebookOpen) return;

        ComputePlayerCameraPose(input);

        bobberTracker.ComputePose(input.playerModel, cameraTransform,
            smoothXAngle, smoothYAngle,
            bobberFollowHeight, bobberFollowDistance,
            bobberOrbitWithMouse, bobberCameraWaterClearance);

        // Dedicated reel-press return takes priority over the blend system. Currently no caller
        // ever flips IsReturningToPlayer to true; kept for forward compatibility.
        if (bobberTracker.IsReturningToPlayer)
        {
            bobberTracker.AdvanceReturnT(bobberReturnDuration);
            if (!bobberTracker.IsReturningToPlayer)
            {
                cameraTransform.position = playerCameraPose.position;
                cameraTransform.rotation = playerCameraPose.rotation;
            }
            else
            {
                float rt = bobberTracker.ReturnT;
                float t = rt * rt * (3f - 2f * rt); // smoothstep ease
                var from = bobberTracker.ReturnFromPose;
                cameraTransform.position = Vector3.Lerp(from.position, playerCameraPose.position, t);
                cameraTransform.rotation = Quaternion.Slerp(from.rotation, playerCameraPose.rotation, t);
            }
            UpdateFov(input);
            return;
        }

        bobberTracker.TickBlend(bobberFollowLerpSpeed);

        if (bobberTracker.HasBobberCameraPose && bobberTracker.BlendValue > 0.001f)
        {
            var bp = bobberTracker.CurrentBobberPose;
            cameraTransform.position = Vector3.Lerp(playerCameraPose.position, bp.position, bobberTracker.BlendValue);
            cameraTransform.rotation = Quaternion.Slerp(playerCameraPose.rotation, bp.rotation, bobberTracker.BlendValue);
        }
        else
        {
            cameraTransform.position = playerCameraPose.position;
            cameraTransform.rotation = playerCameraPose.rotation;
            bobberTracker.ReleaseIfFadedOut();
        }

        UpdateFov(input);
    }

    private void ComputePlayerCameraPose(CameraInput input)
    {
        if (float.IsNaN(xVel) || float.IsNaN(yVel) || float.IsNaN(distanceVelocity)) { xVel = 0f; yVel = 0f; distanceVelocity = 0f; }

        bool isDialogueCamera = input.areControlsLocked && DialogueManager.Instance != null && DialogueManager.Instance.IsDialogueActive();
        bool isBoardCamera = input.areControlsLocked && input.isBountyBoardActive && input.activeBountyBoard != null;
        // Fishing locks player controls, but while the bobber/lure camera owns the view the
        // mouse must still orbit — the bobber pose is built from these same angles, so this is
        // what lets the player rotate around the bobber exactly like around the player.
        bool bobberCameraDominant = bobberTracker.BobberCameraDominant;

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
        else if (!input.isFightingFish && !isCatchCameraActive
                 && (!input.areControlsLocked || bobberCameraDominant))
        {
            // Returning-to-player is the one in-water case where orbit input is suppressed so the
            // camera doesn't fight the one-shot return lerp.
            if (!bobberTracker.IsReturningToPlayer)
            {
                float mouseX = Input.GetAxis("Mouse X") * cameraSpeed * Time.deltaTime;
                float mouseY = Input.GetAxis("Mouse Y") * cameraSpeed * Time.deltaTime;
                Vector2 stickLook = GamepadInput.Look;
                cameraXAngle += mouseX + stickLook.x * gamepadLookSpeedX * Time.deltaTime;
                cameraYAngle -= mouseY + stickLook.y * gamepadLookSpeedY * Time.deltaTime;
                cameraYAngle = Mathf.Clamp(cameraYAngle, cameraYClamp.x, cameraYClamp.y);
            }
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

        reactionTracker.Tick(nibblePitchOffset, nibblePitchLerpSpeed, chargeSmoothTime, chargeMaxSpeed);
        float chargeProgress = reactionTracker.ChargeProgress;
        float chargePitchOffset = -chargePitchUpAngle * chargeProgress;

        smoothXAngle = Mathf.SmoothDampAngle(smoothXAngle, cameraXAngle, ref xVel, cameraSmoothTime);
        smoothYAngle = Mathf.SmoothDampAngle(smoothYAngle, cameraYAngle + reactionTracker.CurrentNibblePitchOffset + chargePitchOffset, ref yVel, cameraSmoothTime);

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

        fightFraming.Apply(
            ref playerCameraPose.position, ref playerCameraPose.rotation,
            input.isFightingFish, input.playerModel, input.activeBobberTransform,
            fightShoulderSide, fightShoulderBack, fightShoulderHeight,
            fightFramingAimT, fightFramingLerpSpeed);
    }

    private void UpdateFov(CameraInput input)
    {
        fovTracker.Tick(cameraTransform, input.isSprinting,
            biteFovZoom, biteFovLerpSpeed, sprintFovBoost, sprintFovLerpSpeed);
    }
}
