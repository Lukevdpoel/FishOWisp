using UnityEngine;
using UnityEngine.UI;

public class FishRipple : MonoBehaviour
{
    public enum FishState { Wandering, Attracted, Scared, Nibbling }

    [Header("Movement")]
    public float swimSpeed = 1.5f;
    public float attractSpeed = 1.2f;
    public float scareSpeed = 5f;
    public float wanderRadius = 3f;

    [Header("Natural Curiosity")]
    [Tooltip("How strongly the fish drifts toward the bobber while wandering (0 = none, 1 = strong).")]
    [Range(0f, 1f)]
    public float naturalAttraction = 0.05f;
    [Tooltip("Fish won't naturally drift closer than this distance to the bobber.")]
    public float naturalAttractionMinDist = 5f;

    [Header("Attracted Movement")]
    [Tooltip("How far the fish weaves side-to-side while approaching the bobber.")]
    public float weaveAmplitude = 2f;
    [Tooltip("How often the fish changes its weave direction.")]
    public float weaveIntervalMin = 0.5f;
    public float weaveIntervalMax = 1.5f;
    [Tooltip("Fish pauses briefly while approaching. Min seconds.")]
    public float attractPauseMin = 0.3f;
    [Tooltip("Fish pauses briefly while approaching. Max seconds.")]
    public float attractPauseMax = 1.2f;
    [Tooltip("Chance each weave cycle that the fish pauses.")]
    [Range(0f, 1f)]
    public float attractPauseChance = 0.3f;

    [Header("Lose Interest")]
    [Tooltip("Seconds without an attract press before the fish can lose interest.")]
    public float loseInterestDelay = 3f;
    [Tooltip("Once eligible, chance per second the fish loses interest and drifts back.")]
    public float loseInterestChancePerSec = 0.25f;
    [Tooltip("How far the fish drifts away when it loses interest.")]
    public float loseInterestDriftDistance = 3f;

    [Header("Wake")]
    [Tooltip("Wake/trail prefab that follows the fish on the water surface.")]
    public GameObject wakePrefab;

    [Header("Obstacle Avoidance")]
    [Tooltip("Layers that count as obstacles fish cannot swim through.")]
    public LayerMask obstacleLayers;
    [Tooltip("How high above the water surface to start the raycast from.")]
    public float obstacleRayHeight = 5f;

    [Header("Ranges")]
    [Tooltip("Distance at which the lead fish starts nibbling the bobber. Keep small so the fish has to almost touch the bobber to bite.")]
    public float nibbleRange = 0.5f;
    [Tooltip("If the player presses Attract while the fish is closer than this, the fish spooks. Keep ≥ nibbleRange.")]
    public float scareRange = 1.0f;
    [Tooltip("How far wandering fish stay from the bobber when another fish is being attracted.")]
    public float bobberAvoidRadius = 3f;
    [Tooltip("Distance at which a non-lead 'follower' fish hovers from the bobber. Should be slightly larger than nibbleRange so followers crowd in but never bite.")]
    public float followerHoverDistance = 1.4f;

    [Header("Awareness (Passive)")]
    [Tooltip("This fish notices the bobber and auto-attracts (as a follower) whenever the bobber sits within this radius. Drifts back to wandering once the bobber leaves the radius.")]
    public float actionRadius = 8f;

    [Header("Aim Glimpse Indicator")]
    [Tooltip("Maximum distance from the camera at which the fish glimpse can appear while aiming. Set 0 to disable the distance check.")]
    public float aimRevealRange = 15f;
    [Tooltip("Minimum seconds between glimpses of this fish while aiming.")]
    public float glimpseIntervalMin = 1.5f;
    [Tooltip("Maximum seconds between glimpses of this fish while aiming.")]
    public float glimpseIntervalMax = 4f;
    [Tooltip("How long a single glimpse stays fully visible (seconds), not counting fades.")]
    public float glimpseDurationMin = 0.25f;
    [Tooltip("How long a single glimpse stays fully visible (seconds), not counting fades.")]
    public float glimpseDurationMax = 0.6f;
    [Tooltip("Seconds the glimpse takes to fade in.")]
    public float glimpseFadeIn = 0.35f;
    [Tooltip("Seconds the glimpse takes to fade out.")]
    public float glimpseFadeOut = 0.6f;

    [Header("Timing")]
    public float scareCooldown = 5f;
    public float wanderPauseMin = 0.5f;
    public float wanderPauseMax = 2f;

    [HideInInspector] public FishPreset preset;

    public FishState CurrentState => currentState;
    public bool IsFollower => isFollower;

    private FishState currentState = FishState.Wandering;
    private bool isFollower;
    private Collider zoneBounds;
    private float waterSurfaceY;
    private Transform bobberTransform;

    // Wandering
    private Vector3 wanderTarget;
    private bool hasWanderTarget;
    private float wanderPauseTimer;

    // Attracted weaving
    private Vector3 weaveOffset;
    private float weaveTimer;
    private float attractPauseTimer;
    private float lastAttractInputTime;
    private bool isDriftingBack;
    private Vector3 driftTarget;

    // Scared
    private Vector3 scareDirection;
    private float scareTimer;

    // Bobber avoidance
    private bool shouldAvoidBobber;

    // Wake
    private GameObject activeWakeInstance;

    // UI indicator
    private GameObject indicatorInstance;
    private Canvas indicatorCanvas;
    private CanvasGroup indicatorCanvasGroup;
    private bool isAimingActive;
    private float glimpseTimer;
    private float glimpseAlpha;
    private enum GlimpsePhase { Hidden, FadingIn, Holding, FadingOut }
    private GlimpsePhase glimpsePhase = GlimpsePhase.Hidden;

    public void Initialize(Collider bounds, float surfaceY, GameObject aimIndicatorPrefab)
    {
        zoneBounds = bounds;
        waterSurfaceY = surfaceY;

        // Place at a random point within the zone
        Vector3 startPos = GetRandomPointInBounds();
        startPos.y = waterSurfaceY;
        transform.position = startPos;

        // Create aim indicator
        if (aimIndicatorPrefab != null)
        {
            indicatorInstance = Instantiate(aimIndicatorPrefab, transform);
            indicatorInstance.transform.localPosition = Vector3.zero;

            indicatorCanvas = indicatorInstance.GetComponentInChildren<Canvas>();
            if (indicatorCanvas != null)
            {
                indicatorCanvas.overrideSorting = true;
                indicatorCanvas.sortingOrder = 1000;
            }

            indicatorCanvasGroup = indicatorInstance.GetComponentInChildren<CanvasGroup>();
            if (indicatorCanvasGroup == null && indicatorCanvas != null)
                indicatorCanvasGroup = indicatorCanvas.gameObject.AddComponent<CanvasGroup>();

            FishAimIndicator aimIndicator = indicatorInstance.GetComponentInChildren<FishAimIndicator>(true);
            if (aimIndicator != null) aimIndicator.ApplyPreset(preset);

            if (indicatorCanvasGroup != null) indicatorCanvasGroup.alpha = 0f;
            indicatorInstance.SetActive(false);
        }

        // Spawn wake
        if (wakePrefab != null)
        {
            Vector3 wakePos = new Vector3(startPos.x, waterSurfaceY, startPos.z);
            activeWakeInstance = Instantiate(wakePrefab, wakePos, wakePrefab.transform.rotation);
        }

        PickNewWanderTarget();
    }

    public void SetWaterSurfaceY(float y)
    {
        waterSurfaceY = y;
    }

    public void SetAvoidBobber(bool avoid)
    {
        shouldAvoidBobber = avoid;
    }

    public void SetBobberTransform(Transform bobber)
    {
        bobberTransform = bobber;
    }

    public void ClearBobberTransform()
    {
        bobberTransform = null;
        isFollower = false;
        if (currentState == FishState.Attracted)
        {
            currentState = FishState.Wandering;
            PickNewWanderTarget();
        }
    }

    public void AttractToBobber()
    {
        if (currentState == FishState.Scared || currentState == FishState.Nibbling) return;
        if (bobberTransform == null) return;

        float dist = GetHorizontalDistance(transform.position, bobberTransform.position);

        if (dist < scareRange)
        {
            Scare();
            return;
        }

        currentState = FishState.Attracted;
        weaveTimer = 0f;
        weaveOffset = Vector3.zero;
        attractPauseTimer = 0f;
        lastAttractInputTime = Time.time;
        isDriftingBack = false;
    }

    public void SetFollower(bool follower)
    {
        isFollower = follower;
    }

    public void StopFollowing()
    {
        if (!isFollower) return;
        isFollower = false;

        if (currentState == FishState.Attracted)
        {
            currentState = FishState.Wandering;
            // Drift to a wander target away from the bobber so the school visually scatters.
            if (bobberTransform != null)
            {
                Vector3 awayDir = transform.position - bobberTransform.position;
                awayDir.y = 0f;
                if (awayDir.sqrMagnitude < 0.01f)
                {
                    Vector2 rand = Random.insideUnitCircle.normalized;
                    awayDir = new Vector3(rand.x, 0f, rand.y);
                }
                else
                {
                    awayDir.Normalize();
                }
                wanderTarget = transform.position + awayDir * loseInterestDriftDistance;
                wanderTarget.y = waterSurfaceY;
                wanderTarget = ClampToBounds(wanderTarget);
                hasWanderTarget = true;
            }
            else
            {
                PickNewWanderTarget();
            }
        }
    }

    public void NotifyAttractInput()
    {
        lastAttractInputTime = Time.time;
        isDriftingBack = false;
    }

    public void Scare()
    {
        // Cancel the bobber's nibble sequence if this fish was nibbling
        if (currentState == FishState.Nibbling && bobberTransform != null)
        {
            BobberController bobber = bobberTransform.GetComponent<BobberController>();
            if (bobber != null) bobber.CancelNibbleSequence();
            FishingEvents.OnFishBite -= OnFishBiteHide;
            FishingEvents.OnFishNibble -= OnNibbleJab;
            FishingEvents.OnBiteImminent -= OnBiteImminent;
            isBiteJabbing = false;
        }

        Vector3 awayDir;
        if (bobberTransform != null)
        {
            awayDir = (transform.position - bobberTransform.position);
            awayDir.y = 0;
            awayDir.Normalize();
        }
        else
        {
            Vector2 rand = Random.insideUnitCircle.normalized;
            awayDir = new Vector3(rand.x, 0, rand.y);
        }

        scareDirection = awayDir;
        scareTimer = scareCooldown;
        scareJinkTimer = 0f;
        isFollower = false;
        currentState = FishState.Scared;
        FishingEvents.OnFishScared?.Invoke();
    }

    private void OnEnable()
    {
        FishingEvents.OnStartAiming += ShowIndicator;
        FishingEvents.OnStopAiming += HideIndicator;
    }

    private void OnDisable()
    {
        FishingEvents.OnStartAiming -= ShowIndicator;
        FishingEvents.OnStopAiming -= HideIndicator;
        FishingEvents.OnFishBite -= OnFishBiteHide;
        FishingEvents.OnFishNibble -= OnNibbleJab;
        FishingEvents.OnBiteImminent -= OnBiteImminent;
    }

    private void ShowIndicator()
    {
        isAimingActive = true;
        EnterHidden(Random.Range(glimpseIntervalMin, glimpseIntervalMax));
    }

    private void HideIndicator()
    {
        isAimingActive = false;
        EnterHidden(0f);
    }

    private void EnterHidden(float waitSeconds)
    {
        glimpsePhase = GlimpsePhase.Hidden;
        glimpseTimer = waitSeconds;
        glimpseAlpha = 0f;
        ApplyGlimpseAlpha();
    }

    private void ApplyGlimpseAlpha()
    {
        if (indicatorInstance == null) return;
        if (indicatorCanvasGroup != null) indicatorCanvasGroup.alpha = glimpseAlpha;
        bool shouldBeActive = glimpseAlpha > 0.001f;
        if (indicatorInstance.activeSelf != shouldBeActive)
            indicatorInstance.SetActive(shouldBeActive);
    }

    private bool IsWithinRevealRange()
    {
        if (aimRevealRange <= 0f) return true;
        Camera cam = Camera.main;
        if (cam == null) return true;
        float sqr = (transform.position - cam.transform.position).sqrMagnitude;
        return sqr <= aimRevealRange * aimRevealRange;
    }

    private void UpdateGlimpse()
    {
        if (indicatorInstance == null) return;

        if (!isAimingActive)
        {
            if (glimpseAlpha != 0f) { glimpseAlpha = 0f; ApplyGlimpseAlpha(); }
            return;
        }

        bool inRange = IsWithinRevealRange();
        if (!inRange)
        {
            // Out of range — fade out if currently visible, then idle hidden until back in range.
            if (glimpsePhase == GlimpsePhase.FadingIn || glimpsePhase == GlimpsePhase.Holding)
            {
                glimpsePhase = GlimpsePhase.FadingOut;
            }
            else if (glimpsePhase == GlimpsePhase.Hidden)
            {
                glimpseTimer = Random.Range(glimpseIntervalMin, glimpseIntervalMax);
            }
        }

        glimpseTimer -= Time.deltaTime;

        switch (glimpsePhase)
        {
            case GlimpsePhase.Hidden:
                if (inRange && glimpseTimer <= 0f)
                {
                    glimpsePhase = GlimpsePhase.FadingIn;
                    glimpseTimer = Mathf.Max(0.0001f, glimpseFadeIn);
                }
                break;

            case GlimpsePhase.FadingIn:
            {
                float dur = Mathf.Max(0.0001f, glimpseFadeIn);
                float t = 1f - Mathf.Clamp01(glimpseTimer / dur);
                glimpseAlpha = Mathf.SmoothStep(0f, 1f, t);
                if (glimpseTimer <= 0f)
                {
                    glimpseAlpha = 1f;
                    glimpsePhase = GlimpsePhase.Holding;
                    glimpseTimer = Random.Range(glimpseDurationMin, glimpseDurationMax);
                }
                break;
            }

            case GlimpsePhase.Holding:
                glimpseAlpha = 1f;
                if (glimpseTimer <= 0f)
                {
                    glimpsePhase = GlimpsePhase.FadingOut;
                    glimpseTimer = Mathf.Max(0.0001f, glimpseFadeOut);
                }
                break;

            case GlimpsePhase.FadingOut:
            {
                float dur = Mathf.Max(0.0001f, glimpseFadeOut);
                float t = Mathf.Clamp01(glimpseTimer / dur);
                glimpseAlpha = Mathf.SmoothStep(0f, 1f, t);
                if (glimpseTimer <= 0f)
                {
                    EnterHidden(Random.Range(glimpseIntervalMin, glimpseIntervalMax));
                    return;
                }
                break;
            }
        }

        ApplyGlimpseAlpha();

        if (glimpseAlpha > 0.001f)
        {
            Camera cam = Camera.main;
            if (cam != null)
            {
                indicatorInstance.transform.rotation = Quaternion.LookRotation(
                    indicatorInstance.transform.position - cam.transform.position
                );
            }
        }
    }

    void Update()
    {
        // Keep on water surface
        Vector3 pos = transform.position;
        pos.y = waterSurfaceY;

        // Keep wake following the fish
        if (activeWakeInstance != null)
        {
            activeWakeInstance.transform.position = new Vector3(pos.x, waterSurfaceY, pos.z);
        }
        transform.position = pos;

        UpdateGlimpse();

        switch (currentState)
        {
            case FishState.Wandering:
                UpdateWandering();
                break;
            case FishState.Attracted:
                UpdateAttracted();
                break;
            case FishState.Scared:
                UpdateScared();
                break;
            case FishState.Nibbling:
                UpdateNibbling();
                break;
        }
    }

    private void UpdateWandering()
    {
        // Passive auto-attract: if the bobber is sitting inside this fish's awareness radius
        // (and we're not being told to give the lead fish space), wake up and approach as a follower.
        // Bait mismatch is *not* gated here — wrong-species fish still gather visually so the pond
        // doesn't look empty. Only the bite/lead promotion in FishingZone is gated by bait.
        if (bobberTransform != null && !shouldAvoidBobber && actionRadius > 0f)
        {
            float bobberDist = GetHorizontalDistance(transform.position, bobberTransform.position);
            if (bobberDist <= actionRadius)
            {
                AttractToBobber();
                if (currentState == FishState.Attracted)
                {
                    isFollower = true;          // self-attracted fish never nibble until promoted by FishingZone
                    return;
                }
            }
        }

        if (wanderPauseTimer > 0f)
        {
            wanderPauseTimer -= Time.deltaTime;
            return;
        }

        if (!hasWanderTarget)
        {
            PickNewWanderTarget();
        }

        float dist = GetHorizontalDistance(transform.position, wanderTarget);

        if (dist < 0.3f)
        {
            hasWanderTarget = false;
            wanderPauseTimer = Random.Range(wanderPauseMin, wanderPauseMax);
            return;
        }

        // If another fish is being attracted, steer away from the bobber
        if (shouldAvoidBobber && bobberTransform != null)
        {
            float bobberDist = GetHorizontalDistance(transform.position, bobberTransform.position);
            if (bobberDist < bobberAvoidRadius)
            {
                Vector3 awayFromBobber = (transform.position - bobberTransform.position);
                awayFromBobber.y = 0;
                awayFromBobber.Normalize();
                Vector3 avoidTarget = transform.position + awayFromBobber * swimSpeed * Time.deltaTime;
                avoidTarget.y = waterSurfaceY;
                avoidTarget = ClampToBounds(avoidTarget);
                if (!IsObstacleAt(avoidTarget))
                {
                    transform.position = avoidTarget;
                }
                FaceMovementDirection(avoidTarget);
                hasWanderTarget = false;
                return;
            }
        }

        // Blend wander target toward bobber if nearby (natural curiosity)
        Vector3 moveTarget = wanderTarget;
        if (!shouldAvoidBobber && bobberTransform != null && naturalAttraction > 0f)
        {
            float bobberDist = GetHorizontalDistance(transform.position, bobberTransform.position);
            if (bobberDist > naturalAttractionMinDist)
            {
                Vector3 bobberPos = bobberTransform.position;
                bobberPos.y = waterSurfaceY;
                moveTarget = Vector3.Lerp(wanderTarget, bobberPos, naturalAttraction);
            }
        }

        MoveToward(moveTarget, swimSpeed);
        FaceMovementDirection(moveTarget);
    }

    private void UpdateAttracted()
    {
        if (bobberTransform == null)
        {
            currentState = FishState.Wandering;
            PickNewWanderTarget();
            return;
        }

        Vector3 bobberPos = bobberTransform.position;
        bobberPos.y = waterSurfaceY;

        float dist = GetHorizontalDistance(transform.position, bobberPos);

        // Passive engagement: while the bobber sits inside our awareness radius, suppress lose-interest
        // (the fish stays curious as long as the lure is nearby). If the bobber leaves the radius and
        // we self-attracted as a follower, gracefully drift back to wandering.
        if (actionRadius > 0f && dist <= actionRadius)
        {
            lastAttractInputTime = Time.time;
        }
        else if (actionRadius > 0f && isFollower && dist > actionRadius)
        {
            StopFollowing();
            return;
        }

        // If drifting back after losing interest
        if (isDriftingBack)
        {
            float driftDist = GetHorizontalDistance(transform.position, driftTarget);
            if (driftDist < 0.3f)
            {
                isDriftingBack = false;
            }
            else
            {
                MoveToward(driftTarget, swimSpeed);
                FaceMovementDirection(driftTarget);
                return;
            }
        }

        if (!isFollower && dist < nibbleRange)
        {
            StartNibbling();
            return;
        }

        // Lose interest if player hasn't attracted recently
        float timeSinceAttract = Time.time - lastAttractInputTime;
        if (timeSinceAttract > loseInterestDelay)
        {
            if (Random.value < loseInterestChancePerSec * Time.deltaTime)
            {
                // Drift away from bobber a bit
                Vector3 awayDir = (transform.position - bobberPos);
                awayDir.y = 0;
                if (awayDir.sqrMagnitude > 0.01f) awayDir.Normalize();
                else awayDir = Random.insideUnitSphere; awayDir.y = 0; awayDir.Normalize();

                driftTarget = transform.position + awayDir * loseInterestDriftDistance;
                driftTarget.y = waterSurfaceY;
                driftTarget = ClampToBounds(driftTarget);
                isDriftingBack = true;
                return;
            }
        }

        // Pause briefly sometimes (fish hesitates)
        if (attractPauseTimer > 0f)
        {
            attractPauseTimer -= Time.deltaTime;
            return;
        }

        // Update weave offset periodically
        weaveTimer -= Time.deltaTime;
        if (weaveTimer <= 0f)
        {
            // Pick a perpendicular offset to the bobber direction
            Vector3 toBobber = (bobberPos - transform.position);
            toBobber.y = 0;
            if (toBobber.sqrMagnitude > 0.01f)
            {
                Vector3 perp = Vector3.Cross(toBobber.normalized, Vector3.up);
                weaveOffset = perp * Random.Range(-weaveAmplitude, weaveAmplitude);
            }

            weaveTimer = Random.Range(weaveIntervalMin, weaveIntervalMax);

            // Random chance to pause
            if (Random.value < attractPauseChance)
            {
                attractPauseTimer = Random.Range(attractPauseMin, attractPauseMax);
            }
        }

        // Followers hover at followerHoverDistance from the bobber rather than crowding the hook.
        Vector3 approachCenter = bobberPos;
        if (isFollower)
        {
            Vector3 fromBobber = transform.position - bobberPos;
            fromBobber.y = 0f;
            float fromBobberDist = fromBobber.magnitude;
            Vector3 outward = fromBobberDist > 0.01f
                ? fromBobber / fromBobberDist
                : new Vector3(Mathf.Cos(GetInstanceID()), 0f, Mathf.Sin(GetInstanceID()));
            approachCenter = bobberPos + outward * followerHoverDistance;
        }

        Vector3 targetPos = approachCenter + weaveOffset;
        targetPos.y = waterSurfaceY;

        MoveToward(targetPos, attractSpeed);
        FaceMovementDirection(targetPos);
    }

    private float scareJinkTimer;

    private void UpdateScared()
    {
        scareTimer -= Time.deltaTime;

        float fleePhase = scareCooldown - 1.5f;
        if (scareTimer > fleePhase)
        {
            // Jink direction randomly every so often
            scareJinkTimer -= Time.deltaTime;
            if (scareJinkTimer <= 0f)
            {
                float jinkAngle = Random.Range(-60f, 60f);
                scareDirection = Quaternion.Euler(0, jinkAngle, 0) * scareDirection;
                scareDirection.y = 0;
                scareDirection.Normalize();
                scareJinkTimer = Random.Range(0.15f, 0.35f);
            }

            // Vary speed each frame for a panicked feel
            float burstSpeed = scareSpeed * Random.Range(0.6f, 1.3f);
            Vector3 targetPos = transform.position + scareDirection * burstSpeed * Time.deltaTime;
            targetPos.y = waterSurfaceY;
            targetPos = ClampToBounds(targetPos);

            if (IsObstacleAt(targetPos))
            {
                scareDirection = Vector3.Cross(scareDirection, Vector3.up).normalized;
                if (scareDirection.sqrMagnitude < 0.01f)
                    scareDirection = -scareDirection;
                scareJinkTimer = 0f;
            }
            else
            {
                transform.position = targetPos;
            }

            FaceMovementDirection(transform.position + scareDirection);
        }

        if (scareTimer <= 0f)
        {
            currentState = FishState.Wandering;
            PickNewWanderTarget();
        }
    }

    private void UpdateNibbling()
    {
        if (bobberTransform == null) return;

        Vector3 bobberPos = bobberTransform.position;
        bobberPos.y = waterSurfaceY;

        if (isJabbing)
        {
            // Dart quickly toward the bobber
            transform.position = Vector3.MoveTowards(transform.position, bobberPos, scareSpeed * Time.deltaTime);
            FaceMovementDirection(bobberPos);

            jabTimer -= Time.deltaTime;
            if (jabTimer <= 0f)
            {
                isJabbing = false;
            }
        }
        else if (!isBiteJabbing)
        {
            // Drift back to hover position (but not if the bite is imminent)
            nibbleHoverPos.y = waterSurfaceY;
            transform.position = Vector3.MoveTowards(transform.position, nibbleHoverPos, jabReturnSpeed * Time.deltaTime);
            FaceMovementDirection(bobberPos);
        }
    }

    private float nibbleHoverDist;
    private Vector3 nibbleHoverPos;
    private bool isJabbing;
    private float jabTimer;
    private float jabDuration = 0.15f;
    private float jabReturnSpeed = 6f;

    private void StartNibbling()
    {
        currentState = FishState.Nibbling;

        // Hover from wherever the fish currently is
        nibbleHoverPos = transform.position;
        nibbleHoverPos.y = waterSurfaceY;
        isJabbing = false;

        BobberController bobber = bobberTransform != null ? bobberTransform.GetComponent<BobberController>() : null;
        if (bobber != null)
        {
            bobber.StartNibbleSequence(preset);
        }

        FishingEvents.OnFishBite += OnFishBiteHide;
        FishingEvents.OnFishNibble += OnNibbleJab;
        FishingEvents.OnBiteImminent += OnBiteImminent;
    }

    private void OnBiteImminent(BobberController bobber)
    {
        if (bobberTransform != null && bobber.transform == bobberTransform)
        {
            MarkBiteImminent();
            FishingEvents.OnBiteImminent -= OnBiteImminent;
        }
    }

    private void PickNibbleHoverPos()
    {
        if (bobberTransform == null) return;

        // Pick a random angle around the bobber to hover at
        float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
        Vector3 offset = new Vector3(Mathf.Cos(angle) * nibbleHoverDist, 0f, Mathf.Sin(angle) * nibbleHoverDist);
        nibbleHoverPos = bobberTransform.position + offset;
        nibbleHoverPos.y = waterSurfaceY;
    }

    private void OnNibbleJab(BobberController bobber)
    {
        if (currentState != FishState.Nibbling) return;
        if (bobberTransform == null || bobber.transform != bobberTransform) return;

        isJabbing = true;
        jabTimer = jabDuration;
    }

    private bool isBiteJabbing;

    public void MarkBiteImminent()
    {
        // Called right before the final nibble — prevents pull-back
        isBiteJabbing = true;
    }

    private void OnFishBiteHide(BobberController bobber)
    {
        if (bobberTransform != null && bobber.transform == bobberTransform)
        {
            HideVisuals();
            FishingEvents.OnFishBite -= OnFishBiteHide;
            FishingEvents.OnFishNibble -= OnNibbleJab;
        }
    }

    private void HideVisuals()
    {
        // Disable all particle systems on this object and children
        var particles = GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < particles.Length; i++)
        {
            particles[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        // Disable all renderers on this object and children
        var renderers = GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            renderers[i].enabled = false;
        }

        // Hide indicator
        if (indicatorInstance != null)
            indicatorInstance.SetActive(false);

        // Destroy wake
        if (activeWakeInstance != null)
        {
            Destroy(activeWakeInstance);
            activeWakeInstance = null;
        }
    }

    private void MoveToward(Vector3 target, float speed)
    {
        Vector3 current = transform.position;
        target.y = waterSurfaceY;
        current.y = waterSurfaceY;

        Vector3 newPos = Vector3.MoveTowards(current, target, speed * Time.deltaTime);
        newPos = ClampToBounds(newPos);
        newPos.y = waterSurfaceY;

        if (IsObstacleAt(newPos))
        {
            // Can't go there — pick a new wander target if wandering
            if (currentState == FishState.Wandering)
            {
                hasWanderTarget = false;
            }
            return;
        }

        transform.position = newPos;
    }

    private void FaceMovementDirection(Vector3 target)
    {
        Vector3 dir = target - transform.position;
        dir.y = 0;
        if (dir.sqrMagnitude > 0.001f)
        {
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                Quaternion.LookRotation(dir),
                Time.deltaTime * 5f
            );
        }
    }

    private void PickNewWanderTarget()
    {
        wanderTarget = GetRandomPointInBounds();
        wanderTarget.y = waterSurfaceY;
        hasWanderTarget = true;
    }

    private Vector3 GetRandomPointInBounds()
    {
        if (zoneBounds == null) return transform.position;

        Bounds b = zoneBounds.bounds;
        for (int i = 0; i < 30; i++)
        {
            Vector3 point = new Vector3(
                Random.Range(b.min.x, b.max.x),
                waterSurfaceY,
                Random.Range(b.min.z, b.max.z)
            );

            // Verify the point is actually inside the collider (handles non-box shapes)
            Vector3 closest = zoneBounds.ClosestPoint(point);
            if (GetHorizontalDistance(point, closest) < 0.1f && !IsObstacleAt(point))
                return point;
        }

        // Fallback to center
        return new Vector3(b.center.x, waterSurfaceY, b.center.z);
    }

    private bool IsObstacleAt(Vector3 pos)
    {
        if (obstacleLayers == 0) return false;
        Vector3 rayOrigin = new Vector3(pos.x, waterSurfaceY + obstacleRayHeight, pos.z);
        return Physics.Raycast(rayOrigin, Vector3.down, obstacleRayHeight + 1f, obstacleLayers);
    }

    private Vector3 ClampToBounds(Vector3 pos)
    {
        if (zoneBounds == null) return pos;
        Vector3 closest = zoneBounds.ClosestPoint(pos);
        closest.y = pos.y;
        return closest;
    }

    private float GetHorizontalDistance(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    void OnDestroy()
    {
        if (activeWakeInstance != null) Destroy(activeWakeInstance);
    }
}
