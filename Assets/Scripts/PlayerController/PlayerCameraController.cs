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

    [Header("Field of View")]
    [Tooltip("Authoritative base FOV for gameplay. Set explicitly so the Cinemachine vcams' FOV (used " +
             "for the menu reveal/handoff) can't leak in and change the game's look. Sprint and bite " +
             "zoom are applied relative to this. The original pre-Cinemachine value was 38.")]
    [SerializeField] private float gameplayFieldOfView = 38f;

    [Header("Aim Camera Settings")]
    [Tooltip("Units the camera pulls IN from its resting distance while aiming (right mouse). Relative " +
             "rather than absolute so it always zooms in regardless of the resting distance the camera " +
             "inherits from the Cinemachine handoff at startup. Clamped to chargeMinDistance so it never " +
             "crosses the player.")]
    [SerializeField] private float aimZoomInAmount = 2.5f;

    [Header("Manual Zoom (scroll wheel)")]
    [Tooltip("Metres of camera distance added/removed per scroll-wheel notch. Wheel up = zoom in.")]
    [SerializeField] private float zoomStep = 1f;
    [Tooltip("Closest the PLAYER orbit camera can be manually zoomed in (m from the pivot).")]
    [SerializeField] private float playerZoomMinDistance = 2f;
    [Tooltip("Furthest the PLAYER orbit camera can be manually zoomed out (m from the pivot).")]
    [SerializeField] private float playerZoomMaxDistance = 12f;
    [Tooltip("Closest the BOBBER/LURE camera can be manually zoomed in (m from the bobber pivot).")]
    [SerializeField] private float bobberZoomMinDistance = 1.5f;
    [Tooltip("Furthest the BOBBER/LURE camera can be manually zoomed out (m from the bobber pivot).")]
    [SerializeField] private float bobberZoomMaxDistance = 8f;

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
    [Tooltip("SmoothDamp time (seconds) for the point the follow camera orbits around. The follow now starts at the throw, so the camera would otherwise chase the fast flight arc exactly. Higher = the camera trails the bobber more loosely and stays stable/straight in the air; it still settles precisely once the bobber lands and stops. ~0.25–0.4 keeps the airborne ride steady. 0 = track the bobber exactly (old behavior).")]
    [SerializeField] private float bobberFollowPositionSmoothTime = 0.3f;
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

    // Player-chosen bobber orbit radius (scroll wheel). -1 = untouched, use bobberFollowDistance.
    // Kept for the whole session so the chosen framing carries across casts.
    private float zoomedBobberDistance = -1f;
    private float ZoomedBobberDistance => zoomedBobberDistance > 0f ? zoomedBobberDistance : bobberFollowDistance;

    // Resting orbit distance captured at the Cinemachine handoff (the HUTBUILT menu's PlayerFollow
    // pose). Static so it survives scene loads: the player is rebuilt per scene, and scenes entered
    // later have no menu / no PlayerFollow vcam, so their Start() would otherwise fall back to the
    // prefab Main Camera's offset — which sits further out than the tuned handoff framing, making the
    // camera "zoom back out" on every scene change. Reusing the captured distance keeps every scene
    // at the same framing the player saw after the intro. -1 = not captured yet this session.
    private static float persistedRestingDistance = -1f;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetPersistedRestingDistance() => persistedRestingDistance = -1f;
    private Vector3 currentPivotPosition;
    private Vector3 pivotVelocity;

    private readonly CameraFovTracker fovTracker = new CameraFovTracker();
    private readonly CameraReactionTracker reactionTracker = new CameraReactionTracker();
    private readonly BobberCameraTracker bobberTracker = new BobberCameraTracker();
    private readonly FightFramingHelper fightFraming = new FightFramingHelper();

    // Player-side orbit pose; bobber side lives in bobberTracker.
    private CameraPose playerCameraPose;

    // One-shot ease for an external handoff (the main menu). After a Cinemachine blend ends on an
    // approximate behind-player vcam, the first frames of gameplay would otherwise SNAP to the
    // reconstructed orbit pose. While this is active, the final written transform is lerped from the
    // captured handoff pose into the live orbit pose so the takeover is smooth.
    private bool handoffActive;
    private float handoffT;
    private float handoffDuration;
    private Vector3 handoffStartPos;
    private Quaternion handoffStartRot;

    // Smoothed over-shoulder fight pose. Ramps from the orbit pose into the framing pose so the
    // transition into a fish fight is smooth instead of a snap.

    public Transform CameraTransform => (useStaticCamera && staticCameraTarget != null) ? staticCameraTarget : cameraTransform;

    // True while the in-water bobber/lure camera owns the view — the mouse is orbiting the
    // bobber, so gameplay cursor rules (locked + hidden) should apply even though player
    // controls are locked for fishing.
    public bool IsBobberCameraActive => bobberTracker.BobberCameraDominant;

    // True while the catch showcase camera is framing a just-caught fish. Like the bobber camera,
    // fishing owns the view here, so the cursor should stay locked + hidden.
    public bool IsCatchCameraActive => isCatchCameraActive;

    public void SetCatchCamera(bool active) => isCatchCameraActive = active;

    [Header("Aim Fish Tracking")]
    [Tooltip("How fast (per second) the aim camera turns to keep a zoom-researched fish framed while aiming. Higher = snappier follow.")]
    [SerializeField] private float aimTrackLerpSpeed = 4f;
    [Tooltip("Extra height (m) the orbit pivot rises while tracking a fish, so the camera looks DOWN over the player at the fish instead of through the player body (which sits at the pivot and would otherwise obscure the fish). 0 = no lift.")]
    [SerializeField] private float aimTrackVerticalOffset = 1.5f;
    [Tooltip("SmoothDamp time (s) for the tracking pivot lift easing in/out, so it doesn't pop when aim/lock starts or ends.")]
    [SerializeField] private float aimTrackLiftSmoothTime = 0.3f;
    [Tooltip("Downward-pitch ceiling (deg) used ONLY while tracking a fish, replacing the normal cameraYClamp.y. The fish sits below the raised pivot, so tracking needs to look down steeper than normal orbit allows — without this the fish gets pinned low in frame.")]
    [SerializeField] private float aimTrackMaxPitch = 78f;
    [Tooltip("Aim this far BELOW the fish (m) while tracking, which lifts the fish higher in the frame (pairs with the player sitting low on screen). 0 = centre on the fish; negative = push the fish lower.")]
    [SerializeField] private float aimTrackFramingHeight = 0.6f;
    [Tooltip("Degrees subtracted from base FOV while tracking a fish — leans the view in on the locked fish. 0 = no FOV zoom.")]
    public float aimTrackFovZoom = 8f;
    [Tooltip("How fast (per second) the FOV interpolates toward the tracking zoom and back. Lower = gentler/slower zoom.")]
    public float aimTrackFovLerpSpeed = 3.5f;
    [Tooltip("Seconds the fish lock must be held before the FOV zoom STARTS, so it doesn't fire while the camera is still swinging into the track (which looks abrupt). The pivot/angle move happens first, then the FOV eases in.")]
    public float aimTrackFovDelay = 0.45f;

    [Header("Cast Aim Camera")]
    [Tooltip("How fast (per second) the camera yaw turns to stay behind the player while aiming a cast — the model itself is turning toward the marker, so the view swings horizontally with the aim.")]
    [SerializeField] private float castAimYawFollowSpeed = 5f;
    [Tooltip("Marker distance (m) at/beyond which no extra look-down is added while aiming a cast.")]
    [SerializeField] private float castAimLiftFarDistance = 7f;
    [Tooltip("Marker distance (m) at which the full close-marker look-down is reached.")]
    [SerializeField] private float castAimLiftNearDistance = 2.5f;
    [Tooltip("Extra look-down pitch (deg) as the marker nears the player, raising the camera over the model so a reticle right in front of the player isn't hidden behind them. This is the ONLY vertical camera motion during a cast aim.")]
    [SerializeField] private float castAimClosePitch = 22f;
    [Tooltip("SmoothDamp time (s) for the close-marker lift easing in/out.")]
    [SerializeField] private float castAimLiftSmoothTime = 0.35f;
    [Tooltip("Sideways shift (m) of the orbit pivot while aiming a cast — positive frames the player toward the LEFT of the screen (over-the-right-shoulder view), keeping the reticle clear of the player model. Eases in/out with the same smoothing as the close-marker lift.")]
    [SerializeField] private float castAimShoulderOffset = 1.6f;
    [Tooltip("Seconds after a throw over which look input ramps back to full strength. The whip gesture's follow-through (mouse/stick still moving as the cast fires) would otherwise yank the freshly unlocked orbit camera.")]
    [SerializeField] private float postThrowLookRecoverTime = 0.7f;

    // The cast-aim controller on this player; the camera reads IsAiming + the marker point from it.
    private RodCasting rodCasting;
    private float currentCastAimPitchLift;
    private float castAimPitchLiftVel;
    private float currentCastAimShoulder;
    private float castAimShoulderVel;
    private float lastThrowTime = -999f;

    // Set by FishResearchScanner while the player is aiming and locked onto a fish: the aim camera
    // gently turns to keep this fish framed until aim is released (then cleared with null).
    private Transform aimTrackTarget;
    public void SetAimTrackTarget(Transform fish) => aimTrackTarget = fish;

    // Eased current value of the tracking pivot lift (toward aimTrackVerticalOffset while tracking,
    // back to 0 otherwise), so the rise/return is smooth.
    private float currentAimTrackLift;
    private float aimTrackLiftVel;
    // How long the current fish lock has been held while aiming, used to delay the FOV zoom start.
    private float aimTrackFovElapsed;

    private void OnEnable()
    {
        if (rodCasting == null)
        {
            Transform root = transform.root != null ? transform.root : transform;
            rodCasting = root.GetComponentInChildren<RodCasting>(includeInactive: true);
            if (rodCasting == null) rodCasting = FindFirstObjectByType<RodCasting>(FindObjectsInactive.Include);
        }

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

        FishingEvents.OnBobberLaunched += HandleBobberLaunched;
        FishingEvents.OnStartReeling += HandleStartReeling;
        // The bobber-follow rig deliberately survives the hook-set (no OnHookFishSuccess
        // teardown): the fish fight is played AS the fish now, so the camera stays framed on
        // it for the whole fight. It fades out at the catch reel (OnStartReeling) — where the
        // catch camera takes over — or on a fail/cancel.
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

        FishingEvents.OnBobberLaunched -= HandleBobberLaunched;
        FishingEvents.OnStartReeling -= HandleStartReeling;
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
    private void HandleChargeReleased(Vector3 dir, float force)
    {
        reactionTracker.ClearChargeProgress();
        // Start the post-throw look-input ramp: the whip's follow-through is still in the
        // player's hand this exact frame.
        lastThrowTime = Time.time;
    }
    private bool hasLoggedChargeEvent;

    private void HandleFishNibble(BobberController b) { reactionTracker.OnNibble(nibblePitchHoldTime); }
    private void HandleFishBite(BobberController b) { fovTracker.OnBiteStart(); reactionTracker.OnBite(); }
    private void HandleBiteRelease() { fovTracker.OnBiteRelease(); }

    private void HandleBobberLaunched(BobberController b) => bobberTracker.BeginFollow(b);

    // Empty-line reel: tear the bobber rig down. Hooked-fish path keeps the rig live until the
    // catch camera takes over.
    private void HandleStartReeling()
    {
        if (!bobberTracker.HasFollowTarget) return;
        if (!bobberTracker.HasHookedFish) bobberTracker.HardClear();
    }

    private void HandleBobberFollowEnd() => bobberTracker.OnFollowEnd();

    // measureFromPivot=false (normal Start): seed the orbit from the prefab-placed Main Camera —
    // yaw/pitch from its rotation, resting distance to the player origin — matching how the camera
    // has always been tuned.
    //
    // measureFromPivot=true (the Cinemachine handoff): the camera is sitting at the PlayerFollow
    // vcam's pose, which is an ARBITRARY pose that does NOT look dead-on at the player. An orbit
    // camera can only sit on a sphere around the pivot looking inward, and it rebuilds its position
    // as pivot + direction*distance where `direction` comes from the yaw/pitch. So we must derive
    // those angles from the camera's POSITION relative to the pivot (not its rotation): the
    // pivot->camera direction. Then pivot + direction*distance reproduces the vcam's POSITION exactly
    // and the handover doesn't jump. (Deriving the angles from the vcam's ROTATION instead — the old
    // behavior — only lands back on the vcam if it looked straight at the pivot; otherwise the camera
    // snaps sideways by the framing offset, which is the bug this fixes.) The look direction settles
    // to "at the pivot", which is what gameplay orbiting uses anyway; the tiny rotation change is
    // eased by the handoff blend.
    public void Initialize(Transform playerModel, bool measureFromPivot = false)
    {
        if (cameraTransform && playerModel)
        {
            Vector3 pivot = playerModel.position + Vector3.up * pivotHeight;

            if (measureFromPivot)
            {
                Vector3 fromPivot = cameraTransform.position - pivot;
                startDistance = fromPivot.magnitude;
                if (startDistance > 0.0001f)
                {
                    Quaternion lookFromPivot = Quaternion.LookRotation(-fromPivot / startDistance);
                    cameraXAngle = lookFromPivot.eulerAngles.y;
                    cameraYAngle = lookFromPivot.eulerAngles.x;
                    // Remember the handoff framing so scenes entered later reuse it instead of the
                    // (further-out) prefab camera offset.
                    persistedRestingDistance = startDistance;
                }
                else
                {
                    Vector3 ang = cameraTransform.eulerAngles;
                    cameraXAngle = ang.y;
                    cameraYAngle = ang.x;
                }
            }
            else
            {
                // Reuse the distance captured at the intro handoff if we have one; only fall back to
                // the prefab camera offset when this session never ran the menu handoff.
                startDistance = persistedRestingDistance > 0f
                    ? persistedRestingDistance
                    : Vector3.Distance(cameraTransform.position, playerModel.position);
                Vector3 initialCameraAngles = cameraTransform.eulerAngles;
                cameraXAngle = initialCameraAngles.y;
                cameraYAngle = initialCameraAngles.x;
            }

            currentCameraDistance = startDistance;
            smoothXAngle = cameraXAngle;
            smoothYAngle = cameraYAngle;
            fovTracker.Initialize(cameraTransform.GetComponent<Camera>(), gameplayFieldOfView);
            currentPivotPosition = pivot;
            playerCameraPose.position = cameraTransform.position;
            playerCameraPose.rotation = cameraTransform.rotation;
        }
        if (framingTarget == null) framingTarget = playerModel;
    }

    // Seeds the resting orbit pose for the title handoff DIRECTLY from the PlayerFollow vcam's world
    // pose, without moving the live camera (so BeginHandoffBlend can then ease from wherever the
    // flythrough left it into this pose in one continuous move).
    //
    // Unlike Initialize(measureFromPivot:true), this does NOT reconstruct the angles from the
    // camera's position relative to the pivot. At the press the player has only just been enabled and
    // hasn't fallen to the ground yet, so that pivot is transient and position-derived angles land
    // the camera off to the side / not behind. Instead we take the resting ANGLES straight from the
    // vcam's ROTATION — a fixed "behind + looking down" orientation that doesn't depend on where the
    // player is right now — and rebuild the pose as pivot + back*distance each frame, so it stays
    // squarely behind the player as it settles. The DISTANCE is the vcam-to-pivot distance (its
    // horizontal span dominates, so the player's transient height barely moves it) and is persisted
    // so later scenes reuse the same framing.
    public void SeedRestingPoseFromMenuVcam(Transform playerModel, Vector3 vcamPos, Quaternion vcamRot)
    {
        if (cameraTransform == null || playerModel == null) return;

        Vector3 pivot = playerModel.position + Vector3.up * pivotHeight;

        startDistance = Mathf.Max(0.01f, Vector3.Distance(vcamPos, pivot));
        persistedRestingDistance = startDistance;

        Vector3 ang = vcamRot.eulerAngles;
        cameraXAngle = ang.y;
        cameraYAngle = ang.x;

        currentCameraDistance = startDistance;
        smoothXAngle = cameraXAngle;
        smoothYAngle = cameraYAngle;
        currentPivotPosition = pivot;

        // Reconstruct the resting pose the same way UpdateCamera does, so it's the exact target the
        // handoff blend eases toward.
        Quaternion rotation = Quaternion.Euler(smoothYAngle, smoothXAngle, 0f);
        Vector3 cameraDirection = -(rotation * Vector3.forward);
        playerCameraPose.position = pivot + cameraDirection * currentCameraDistance;
        playerCameraPose.rotation = rotation;

        // Set the gameplay base FOV but keep the live (flythrough) FOV so the tracker glides it over
        // during the ease instead of popping on the press.
        var cam = cameraTransform.GetComponent<Camera>();
        float liveFov = cam != null ? cam.fieldOfView : 0f;
        fovTracker.Initialize(cam, gameplayFieldOfView);
        if (cam != null) cam.fieldOfView = liveFov;

        if (framingTarget == null) framingTarget = playerModel;
    }

    // Capture the current (e.g. Cinemachine-blended) camera pose so the next `duration` seconds of
    // UpdateCamera ease from it into the live orbit pose instead of snapping. Call right after the
    // pose is seeded via Initialize(), while the camera still sits at the blend's end pose.
    public void BeginHandoffBlend(float duration)
    {
        if (cameraTransform == null || duration <= 0f) return;
        handoffStartPos = cameraTransform.position;
        handoffStartRot = cameraTransform.rotation;
        handoffDuration = duration;
        handoffT = 0f;
        handoffActive = true;
    }

    // Lerps the just-written transform from the captured handoff pose toward the live orbit pose.
    // Runs at the very end of UpdateCamera, so `cameraTransform` already holds the target pose.
    //
    // Uses an ease-OUT curve (not SmoothStep's ease-in-out): the camera has to START moving the
    // instant the button is pressed and then glide to a gentle stop at the player. SmoothStep's flat
    // ease-in tail meant it crept for the first ~half-second before visibly moving, which read as a
    // long pause before the handoff "kicked in".
    private void ApplyHandoffBlend()
    {
        if (!handoffActive) return;
        handoffT += Time.deltaTime / handoffDuration;
        float u = Mathf.Clamp01(handoffT);
        float s = Mathf.Sin(u * Mathf.PI * 0.5f); // ease-out sine: prompt, steady start; gentle stop
        cameraTransform.position = Vector3.Lerp(handoffStartPos, cameraTransform.position, s);
        cameraTransform.rotation = Quaternion.Slerp(handoffStartRot, cameraTransform.rotation, s);
        if (handoffT >= 1f) handoffActive = false;
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
            bobberFollowHeight, ZoomedBobberDistance,
            bobberOrbitWithMouse, bobberCameraWaterClearance,
            bobberFollowPositionSmoothTime,
            collisionLayers, collisionRadius, zoomDampTime);

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

        ApplyHandoffBlend();

        UpdateFov(input);
    }

    private void ComputePlayerCameraPose(CameraInput input)
    {
        if (float.IsNaN(xVel) || float.IsNaN(yVel) || float.IsNaN(distanceVelocity)) { xVel = 0f; yVel = 0f; distanceVelocity = 0f; }

        // Tracking lift: ease the orbit pivot up while aiming-and-locked on a fish, so the camera
        // looks over the player at the fish (the player sits at the pivot and would block it).
        bool aimTracking = input.isAiming && aimTrackTarget != null;
        float targetLift = aimTracking ? aimTrackVerticalOffset : 0f;
        currentAimTrackLift = Mathf.SmoothDamp(currentAimTrackLift, targetLift, ref aimTrackLiftVel, aimTrackLiftSmoothTime);

        bool isDialogueCamera = input.areControlsLocked && DialogueManager.Instance != null && DialogueManager.Instance.IsDialogueActive();
        bool isBoardCamera = input.areControlsLocked && input.isBountyBoardActive && input.activeBountyBoard != null;
        // Fishing locks player controls, but while the bobber/lure camera owns the view the
        // mouse must still orbit — the bobber pose is built from these same angles, so this is
        // what lets the player rotate around the bobber exactly like around the player.
        bool bobberCameraDominant = bobberTracker.BobberCameraDominant;
        bool castAiming = rodCasting != null && rodCasting.IsAiming;

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
        else if (aimTracking)
        {
            // Zoom research: while aiming and locked on a fish, turn the camera to keep it framed
            // until aim is released. Aim from the LIFTED pivot so the look direction matches the
            // raised orbit centre used below, and aim a touch BELOW the fish so it rides higher in
            // the frame (the player sits low on screen, so the fish wants to be above centre).
            Vector3 pivot = input.playerModel.position + Vector3.up * (pivotHeight + currentAimTrackLift);
            Vector3 lookTarget = aimTrackTarget.position - Vector3.up * aimTrackFramingHeight;
            Vector3 dirToFish = lookTarget - pivot;
            if (dirToFish.sqrMagnitude > 0.0001f)
            {
                Quaternion targetRot = Quaternion.LookRotation(dirToFish.normalized);
                cameraXAngle = Mathf.LerpAngle(cameraXAngle, targetRot.eulerAngles.y, Time.deltaTime * aimTrackLerpSpeed);
                cameraYAngle = Mathf.LerpAngle(cameraYAngle, targetRot.eulerAngles.x, Time.deltaTime * aimTrackLerpSpeed);
                // Steeper down-pitch ceiling than normal orbit so the camera can actually look down
                // at the fish instead of capping at cameraYClamp.y and leaving it low in frame.
                cameraYAngle = Mathf.Clamp(cameraYAngle, cameraYClamp.x, aimTrackMaxPitch);
            }
        }
        else if (castAiming)
        {
            // Aiming a cast: the model is turning toward the marker (RodCasting), so following its
            // yaw swings the view horizontally with the aim. Pitch is deliberately untouched — the
            // only vertical motion while aiming is the close-marker lift added below.
            cameraXAngle = Mathf.LerpAngle(cameraXAngle, input.playerModel.eulerAngles.y,
                                           Time.deltaTime * castAimYawFollowSpeed);
        }
        // Orbit input also stays live through the fish fight while the bobber rig owns the
        // view: steering is on A/D + left stick there, so mouse / right stick are free to
        // rotate around the fish (the rig's pose already handles terrain collision and the
        // water-surface clearance). The auto-yaw fight branch below is only the fallback for
        // a fight with no bobber rig.
        else if (!isCatchCameraActive
                 && (bobberCameraDominant || (!input.isFightingFish && !input.areControlsLocked)))
        {
            // Returning-to-player is the one in-water case where orbit input is suppressed so the
            // camera doesn't fight the one-shot return lerp.
            if (!bobberTracker.IsReturningToPlayer)
            {
                // Right after a throw the whip gesture's follow-through is still on the mouse/
                // stick — ramp look input back in instead of letting that motion yank the view.
                float lookScale = postThrowLookRecoverTime <= 0f ? 1f
                    : Mathf.Clamp01((Time.time - lastThrowTime) / postThrowLookRecoverTime);

                float mouseX = Input.GetAxis("Mouse X") * cameraSpeed * Time.deltaTime;
                float mouseY = Input.GetAxis("Mouse Y") * cameraSpeed * Time.deltaTime;
                Vector2 stickLook = GamepadInput.Look;
                cameraXAngle += (mouseX + stickLook.x * gamepadLookSpeedX * Time.deltaTime) * lookScale;
                cameraYAngle -= (mouseY + stickLook.y * gamepadLookSpeedY * Time.deltaTime) * lookScale;
                cameraYAngle = Mathf.Clamp(cameraYAngle, cameraYClamp.x, cameraYClamp.y);

                ApplyManualZoom(bobberCameraDominant);
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

        // Close-marker lift: as the cast reticle comes in toward the player's feet, ease extra
        // look-down pitch in so the camera rises over the model instead of letting the player
        // body hide the reticle. Eases back out when the marker moves away or the aim ends.
        float liftTarget = 0f;
        if (castAiming)
        {
            Vector3 toMarker = rodCasting.AimMarkerPoint - input.playerModel.position;
            toMarker.y = 0f;
            liftTarget = castAimClosePitch
                       * Mathf.InverseLerp(castAimLiftFarDistance, castAimLiftNearDistance, toMarker.magnitude);
        }
        currentCastAimPitchLift = Mathf.SmoothDamp(currentCastAimPitchLift, liftTarget,
                                                   ref castAimPitchLiftVel, castAimLiftSmoothTime);
        currentCastAimShoulder = Mathf.SmoothDamp(currentCastAimShoulder,
                                                  castAiming ? castAimShoulderOffset : 0f,
                                                  ref castAimShoulderVel, castAimLiftSmoothTime);

        smoothXAngle = Mathf.SmoothDampAngle(smoothXAngle, cameraXAngle, ref xVel, cameraSmoothTime);
        smoothYAngle = Mathf.SmoothDampAngle(smoothYAngle, cameraYAngle + reactionTracker.CurrentNibblePitchOffset + chargePitchOffset + currentCastAimPitchLift, ref yVel, cameraSmoothTime);

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
            // pivotHeight plus the eased tracking lift — raises the orbit centre over the player's
            // head while tracking a fish so the player no longer sits between the camera and the fish.
            targetPivot = basePos + Vector3.up * (pivotHeight + currentAimTrackLift);
            // Over-shoulder shift while aiming a cast: sliding the orbit pivot to the camera's
            // right frames the player off-centre left, so the reticle line-of-sight clears the
            // model. The eased scalar (computed above) takes it in and out smoothly.
            targetPivot += rotation * Vector3.right * currentCastAimShoulder;
            currentPivotPosition = targetPivot;
            pivotVelocity = Vector3.zero;
        }

        float targetDistance = startDistance;
        RaycastHit hit;

        if (isCatchCameraActive) targetDistance = catchZoomDistance;
        else if (input.isAiming) targetDistance = Mathf.Max(chargeMinDistance, startDistance - aimZoomInAmount);

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

    // Scroll-wheel zoom, applied to whichever orbit currently owns the view. Runs only inside the
    // free-orbit input branch, so it is naturally suppressed everywhere orbiting is (dialogue,
    // catch camera, menus, the no-rig fight fallback). One wheel notch reads as ±0.1 from
    // GetAxis, so the delta is normalized to notches before scaling by zoomStep.
    private void ApplyManualZoom(bool bobberCameraDominant)
    {
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) < 0.0001f) return;

        float delta = -(scroll / 0.1f) * zoomStep; // wheel up (positive) pulls the camera in

        if (bobberCameraDominant)
        {
            zoomedBobberDistance = Mathf.Clamp(ZoomedBobberDistance + delta,
                bobberZoomMinDistance, bobberZoomMaxDistance);
        }
        else
        {
            startDistance = Mathf.Clamp(startDistance + delta,
                playerZoomMinDistance, playerZoomMaxDistance);
            // Keep the persisted resting distance in sync so a scene change doesn't snap the
            // camera back to the pre-zoom framing captured at the menu handoff.
            persistedRestingDistance = startDistance;
        }
    }

    private void UpdateFov(CameraInput input)
    {
        bool aimTracking = input.isAiming && aimTrackTarget != null;

        // Delay the FOV zoom until the lock has been held a moment, so it doesn't fire while the
        // camera is still swinging/lifting into the track (which reads as abrupt). Resets when the
        // lock drops, so a fresh lock waits out the delay again.
        aimTrackFovElapsed = aimTracking ? aimTrackFovElapsed + Time.deltaTime : 0f;
        bool fovZoomActive = aimTracking && aimTrackFovElapsed >= aimTrackFovDelay;

        fovTracker.Tick(cameraTransform, input.isSprinting,
            biteFovZoom, biteFovLerpSpeed, sprintFovBoost, sprintFovLerpSpeed,
            fovZoomActive, aimTrackFovZoom, aimTrackFovLerpSpeed);
    }
}
